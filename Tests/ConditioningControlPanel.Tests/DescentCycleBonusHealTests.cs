using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Cycle choice's permanent +10% XP lives on the server; a PC that never ran the ceremony, or
/// whose settings were replaced, must get it back from the sync ack (support ticket, 2026-09-24).
/// </summary>
public class DescentCycleBonusHealTests
{
    [Fact]
    public void ServerCycle_RestoresTheBonus_OnAFreshSettingsFile()
    {
        var s = new AppSettings { DescentCycle = 0, DescentCycleXpBonus = 1.0 };

        Assert.True(ProfileSyncService.EnsureCycleBonus(s, DescentMigrationChoices.Cycle));
        Assert.Equal(1, s.DescentCycle);
        Assert.Equal(DescentMigration.CycleXpBonus, s.DescentCycleXpBonus);
    }

    [Fact]
    public void AlreadyHeld_WritesNothing()
    {
        var s = new AppSettings { DescentCycle = 1, DescentCycleXpBonus = DescentMigration.CycleXpBonus };
        Assert.False(ProfileSyncService.EnsureCycleBonus(s, DescentMigrationChoices.Cycle));
    }

    [Theory]
    [InlineData("restore")]
    [InlineData(null)]
    public void RestoreOrNoChoice_NeverGrants(string? choice)
    {
        var s = new AppSettings { DescentCycle = 0, DescentCycleXpBonus = 1.0 };
        Assert.False(ProfileSyncService.EnsureCycleBonus(s, choice));
        Assert.Equal(1.0, s.DescentCycleXpBonus);
    }
}
