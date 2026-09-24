using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class CompanionPerkMigrationTests
{
    [Fact]
    public void OldProfilesKeepTheirPerkAndSelectingALookDoesNotChangeAnIndependentPerk()
    {
        var selected = CompanionPerks.Resolve(null, CompanionBonusType.AutonomyBonus, true);
        Assert.Equal(CompanionBonusType.AutonomyBonus, selected);
        Assert.Equal(selected, CompanionPerks.Resolve(selected, CompanionBonusType.XPDrain, true));
        Assert.Equal(CompanionBonusType.XPDrain, CompanionPerks.Resolve(selected, CompanionBonusType.XPDrain, false));
    }
    [Fact]
    public void CorruptSavedPerkFallsBackWithoutIntroducingADrain()
    {
        Assert.Equal(CompanionBonusType.PinkFilterBonus,
            CompanionPerks.Resolve((CompanionBonusType)99, CompanionBonusType.PinkFilterBonus, true));
        var settings = new AppSettings { CompanionPerk = (CompanionBonusType)99 };
        Assert.Null(settings.CompanionPerk);
    }
    [Theory]
    [InlineData(CompanionBonusType.PinkFilterBonus, 50)]
    [InlineData(CompanionBonusType.SessionCompletionBonus, 75)]
    [InlineData(CompanionBonusType.AutonomyBonus, 100)]
    [InlineData(CompanionBonusType.XPDrain, 125)]
    [InlineData(CompanionBonusType.StrictModeBonus, 150)]
    public void PerkRetainsItsOriginalUnlockLevel(CompanionBonusType perk, int level)
    {
        Assert.False(CompanionPerks.CanSelect(perk, level - 1));
        Assert.True(CompanionPerks.CanSelect(perk, level));
    }
}
