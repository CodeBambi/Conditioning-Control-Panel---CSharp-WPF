using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Contract tests for the achievement catalogue and the wardrobe's achievement gates.
///
/// Two things can silently break here and neither shows up at build time:
///  1. A cosmetics registry row can name an achievement that does not exist (or one that is
///     parked/IsHidden). WardrobeCatalog reads the gate as a raw string and the gallery simply
///     never shows the achievement, so the item becomes permanently unobtainable with no error.
///  2. The number a tracker branches on can drift away from the number the achievement's
///     Requirement string promises the user.
///
/// The service's instance trackers themselves are deliberately NOT exercised here - see the
/// remark on <see cref="ThresholdsMatchTheRequirementText"/>.
/// </summary>
public class AchievementCatalogTests
{
    /// <summary>
    /// Every <c>"unlock": "achievement:&lt;id&gt;"</c> in the cosmetics registry must name a real,
    /// visible achievement. A gate pointing at an unknown id, or at an IsHidden (parked) one that
    /// no user can earn in this build, locks the art away forever.
    ///
    /// Passes trivially while the registry is still all-"free" - it asserts about whatever gates
    /// exist, not that any exist.
    /// </summary>
    [Fact]
    public void RegistryAchievementGatesNameRealVisibleAchievements()
    {
        var path = FindRegistry();
        Assert.NotNull(path); // the registry ships as Content; missing means the build is broken

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        if (!doc.RootElement.TryGetProperty("items", out var items)) return;

        foreach (var row in items.EnumerateArray())
        {
            if (!row.TryGetProperty("unlock", out var unlockEl)) continue;
            var unlock = unlockEl.GetString();
            if (string.IsNullOrWhiteSpace(unlock)) continue;
            if (!unlock.StartsWith("achievement:", StringComparison.OrdinalIgnoreCase)) continue;

            // Same extraction WardrobeCatalog.WardrobeItem.RequiredAchievementId performs.
            var achId = unlock.Substring("achievement:".Length).Trim();
            var itemId = row.TryGetProperty("id", out var idEl) ? idEl.GetString() : "(no id)";

            Assert.False(string.IsNullOrEmpty(achId),
                $"Registry item '{itemId}' has an empty achievement gate.");
            Assert.True(Achievement.All.ContainsKey(achId),
                $"Registry item '{itemId}' is gated on unknown achievement '{achId}'.");
            Assert.False(Achievement.All[achId].IsHidden,
                $"Registry item '{itemId}' is gated on parked/hidden achievement '{achId}' - unobtainable.");
        }
    }

    /// <summary>
    /// The gate resolution above is only meaningful if it agrees with the catalog the app builds
    /// at runtime, so pin the same invariant through WardrobeCatalog itself.
    /// </summary>
    [Fact]
    public void WardrobeCatalogGatesNameRealVisibleAchievements()
    {
        foreach (var item in ConditioningControlPanel.Services.WardrobeCatalog.Items)
        {
            var gate = item.RequiredAchievementId;
            if (gate == null) continue;
            Assert.True(Achievement.All.ContainsKey(gate),
                $"Wardrobe item '{item.Id}' is gated on unknown achievement '{gate}'.");
            Assert.False(Achievement.All[gate].IsHidden,
                $"Wardrobe item '{item.Id}' is gated on hidden achievement '{gate}'.");
        }
    }

    public static IEnumerable<object[]> NewAchievementIds() => new[]
    {
        new object[] { "screen_time", AchievementCategory.TimeSessions },
        new object[] { "eyes_front", AchievementCategory.Minigames },
        new object[] { "word_perfect", AchievementCategory.Minigames },
        new object[] { "thirty_day_doll", AchievementCategory.TimeSessions },
        new object[] { "threadbare", AchievementCategory.TimeSessions },
        new object[] { "window_shopping", AchievementCategory.Progression },
    };

    /// <summary>
    /// The six cumulative-counter achievements are all free, visible, and counted in the
    /// gallery denominator (GetTotalCount skips IsHidden).
    /// </summary>
    [Theory]
    [MemberData(nameof(NewAchievementIds))]
    public void NewAchievementsAreFreeVisibleAndCategorised(string id, AchievementCategory category)
    {
        Assert.True(Achievement.All.TryGetValue(id, out var a), $"Missing achievement '{id}'.");
        Assert.Equal(id, a!.Id);
        Assert.Equal(category, a.Category);
        Assert.False(a.IsExclusive);
        Assert.False(a.IsHidden);
        Assert.False(string.IsNullOrWhiteSpace(a.Name));
        Assert.False(string.IsNullOrWhiteSpace(a.Requirement));
        Assert.False(string.IsNullOrWhiteSpace(a.FlavorText));
        Assert.Equal($"{id}.png", a.ImageName);
    }

    /// <summary>
    /// Dictionary key and entry Id must agree for every achievement - TryUnlock looks up by key
    /// but the popup, the Discord webhook and the loc keys all read Id.
    /// </summary>
    [Fact]
    public void EveryAchievementKeyMatchesItsId()
    {
        Assert.All(Achievement.All, kv => Assert.Equal(kv.Key, kv.Value.Id));
    }

    /// <summary>
    /// The thresholds the trackers branch on, checked against the numbers the Requirement strings
    /// promise. This is the only level at which the new unlocks can be unit-tested: constructing
    /// an <c>AchievementService</c> is not viable in a test host - the constructor reads and
    /// (via TryUnlock/Save) rewrites the REAL <c>%APPDATA%\ConditioningControlPanel\achievements.json</c>,
    /// starts two DispatcherTimers, and mutates the live login streak through UpdateDailyStreak.
    /// So the tracker methods are exercised by play-test, and the numbers are pinned here.
    /// </summary>
    [Fact]
    public void ThresholdsMatchTheRequirementText()
    {
        // 10 cumulative hours, expressed in minutes by both trackers.
        Assert.Equal(600, AchievementService.ScreenTimeVideoMinutes);
        Assert.Equal(10, AchievementService.ScreenTimeVideoMinutes / 60);
        Assert.Contains("10 cumulative hours", Achievement.All["screen_time"].Requirement);

        Assert.Equal(600, AchievementService.ThreadbareSpiralMinutes);
        Assert.Equal(10, AchievementService.ThreadbareSpiralMinutes / 60);
        Assert.Contains("10 cumulative hours", Achievement.All["threadbare"].Requirement);

        Assert.Equal(100, AchievementService.EyesFrontAttentionChecks);
        Assert.Contains("100", Achievement.All["eyes_front"].Requirement);

        Assert.Equal(50, AchievementService.WordPerfectLockCards);
        Assert.Contains("50", Achievement.All["word_perfect"].Requirement);

        Assert.Equal(30, AchievementService.ThirtyDayDollConsecutiveDays);
        Assert.Contains("30 days", Achievement.All["thirty_day_doll"].Requirement);

        Assert.Equal(100, AchievementService.WindowShoppingPointsSpent);
        Assert.Contains("100", Achievement.All["window_shopping"].Requirement);
    }

    /// <summary>
    /// Thirty-Day Doll must sit strictly above Daily Maintenance (7 days) so the two streak
    /// unlocks in CheckDailyMaintenance can never collapse into one.
    /// </summary>
    [Fact]
    public void StreakThresholdsAreOrdered()
    {
        Assert.True(AchievementService.ThirtyDayDollConsecutiveDays > 7);
    }

    /// <summary>
    /// Prefer the copy beside the test host (the app ships the registry as Content, which flows
    /// through the ProjectReference), then fall back to walking up to the repo source so the test
    /// still means something on a machine where the copy step has not run.
    /// </summary>
    private static string? FindRegistry()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "registry.json");
        if (File.Exists(beside)) return beside;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "ConditioningControlPanel", "Resources", "cosmetics", "registry.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
