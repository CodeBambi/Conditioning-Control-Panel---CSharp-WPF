using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #865 - "my level/XP/skill points reset every time I launch".
///
/// Both sync paths carry an anti-cheat clamp: when LOCAL total XP is far above what the server
/// returned, the client adopts the server values so a hand-edited settings.json cannot persist an
/// inflated profile. The clamp is guarded by "does the server profile look uninitialized?", and
/// that guard used to AND together five conditions - level, XP, achievements, unlocked skills and
/// skill points. Any single non-zero field flipped it to "the server record is real", so a user
/// whose server row had lost its level/XP but still carried one achievement or one banked skill
/// point got clamped to Level 1 / 0 XP. The clamp wrote local settings; the server row never
/// changed; so it fired again on the next launch, and the next.
///
/// The guard now consults level and XP only. These tests pin that: nothing but level/XP may ever
/// influence the answer, and any profile with real progression is never "uninitialized".
/// </summary>
public class ProfileSyncUninitializedGuardTests
{
    [Theory]
    [InlineData(1, 0)]      // pristine record
    [InlineData(0, 0)]      // server omitted level entirely
    [InlineData(1, 99)]     // below the same floor the boot defaults-guard uses
    public void EmptyServerRecordIsUninitialized(int level, double xp)
        => Assert.True(ProfileSyncService.ServerProfileLooksUninitialized(level, xp));

    [Theory]
    [InlineData(2, 0)]        // level says progression even with no XP echoed
    [InlineData(1, 100)]      // exactly at the floor - meaningful
    [InlineData(1, 5_000)]
    [InlineData(47, 250_000)]
    [InlineData(199, 9_000_000)]
    public void AProfileWithMeaningfulLevelOrXpIsNeverUninitialized(int level, double xp)
        => Assert.False(ProfileSyncService.ServerProfileLooksUninitialized(level, xp));

    [Fact]
    public void TheEveryLaunchResetCaseIsDefended()
    {
        // The exact #865 shape: the server row came back emptied of level/XP. Whatever else it
        // carried (a banked skill point, an achievement, an unlocked skill) is not an argument
        // that the account really is at Level 1 - and the old guard treated it as exactly that.
        // Only level/XP are inputs now, so there is no field left that can un-defend this.
        Assert.True(ProfileSyncService.ServerProfileLooksUninitialized(1, 0));
    }

    [Fact]
    public void TheGuardAgreesWithTheBootDefaultsGuardFloor()
    {
        // SyncProfileAsync refuses to PUSH while local is Level<=1 and total XP < 100. If these
        // two floors ever drift apart, one of them starts calling a profile "real" that the other
        // calls "defaults", and the push/adopt pair stops being each other's inverse.
        Assert.Equal(100d, ProfileSyncService.MeaningfulProgressXp);
        Assert.True(ProfileSyncService.ServerProfileLooksUninitialized(1, ProfileSyncService.MeaningfulProgressXp - 1));
        Assert.False(ProfileSyncService.ServerProfileLooksUninitialized(1, ProfileSyncService.MeaningfulProgressXp));
    }
}
