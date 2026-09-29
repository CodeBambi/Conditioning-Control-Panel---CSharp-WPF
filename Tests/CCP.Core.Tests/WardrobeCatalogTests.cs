using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Core wardrobe registry against the shipped Assets/cosmetics/registry.json.</summary>
public sealed class WardrobeCatalogTests
{
    [Fact]
    public void RewardMapReversesTheRegistryGates()
    {
        var gates = WardrobeCatalog.AchievementGates()!;
        var rewards = WardrobeCatalog.AchievementRewards();
        Assert.NotEmpty(rewards);
        Assert.Equal("bambi_bubblegum_scarf", rewards["plastic_initiation"].Id);
        Assert.Equal("Bubblegum Scarf", rewards["plastic_initiation"].Name);
        // Every gated achievement pays out the FIRST item in registry order that it gates.
        foreach (var (achievementId, item) in rewards)
            Assert.Same(WardrobeCatalog.Items.First(i => i.RequiredAchievementId == achievementId), item);
        Assert.Equal(gates.Values.Distinct().OrderBy(x => x), rewards.Keys.OrderBy(x => x));
        Assert.False(rewards.ContainsKey("no_such_achievement"));
    }

    [Fact]
    public void ArtPathStaysInsideTheCosmeticsFolder()
    {
        var path = WardrobeCatalog.ArtPath("bambi_bubblegum_scarf");
        Assert.EndsWith(System.IO.Path.Combine("Resources", "cosmetics", "bambi", "bambi_bubblegum_scarf.png"), path);
        Assert.Null(WardrobeCatalog.ArtPath("no_such_item"));
        Assert.Null(WardrobeCatalog.ArtPath(null));
    }

    [Fact]
    public void GatedItemsFollowTheCurrentUsersProgress()
    {
        var gated = WardrobeCatalog.Find("bambi_bubblegum_scarf");
        var free = WardrobeCatalog.Items.First(i => i.RequiredAchievementId == null);
        var was = WardrobeCatalog.ProgressProvider;
        try
        {
            // No progress loaded yet: gating must never brick the picker.
            WardrobeCatalog.ProgressProvider = null;
            Assert.True(WardrobeCatalog.IsUnlockedForCurrentUser(gated));

            var progress = new AchievementProgress();
            WardrobeCatalog.ProgressProvider = () => progress;
            Assert.False(WardrobeCatalog.IsUnlockedForCurrentUser(gated));
            Assert.True(WardrobeCatalog.IsUnlockedForCurrentUser(free));
            progress.Unlock("plastic_initiation");
            Assert.True(WardrobeCatalog.IsUnlockedForCurrentUser(gated));

            WardrobeCatalog.ProgressProvider = () => throw new InvalidOperationException();
            Assert.True(WardrobeCatalog.IsUnlockedForCurrentUser(gated));
        }
        finally { WardrobeCatalog.ProgressProvider = was; }
    }
}
