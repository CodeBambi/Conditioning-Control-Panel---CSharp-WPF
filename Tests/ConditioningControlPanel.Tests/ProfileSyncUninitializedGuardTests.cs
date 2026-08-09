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

    // NOTE: "not uninitialized" is NOT the same as "safe for the clamp to adopt". The Level 1
    // rows below are pinned here only for this predicate's own contract (and the shared 100 XP
    // floor); the clamp sites refuse them anyway - see ServerProfileTooEmptyToClampTo further
    // down, which is what closes #865 for real.
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

    // ---------------------------------------------------------------------------------------
    // The CLAMP guard: stronger than "uninitialized", and the one the two clamp sites ask.
    //
    // ServerProfileLooksUninitialized narrows #865 but leaves a hole one digit wide. Its XP floor
    // is 100, so a server row emptied down to Level 1 / 150 XP reads as a REAL record — and the
    // clamp fires (local is 75,000 XP ahead), resetting a Level 40 player to Level 1. The server
    // row never changes, so it repeats on every launch: #865 again, just with a survivor.
    //
    // The clamp sites now ask ServerProfileTooEmptyToClampTo, which refuses any Level<=1 record
    // whatever XP rides along. Nothing legitimate lands a 75k-XP-ahead account back on Level 1;
    // a real season zeroing arrives as the explicit level_reset flag on its own branch.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 150)]     // THE regression: old guard called this "real" and clamped
    [InlineData(1, 5_000)]   // and it got worse the more XP the emptied row kept
    [InlineData(1, 99_999)]
    public void ALevelOneRowIsNeverClampedToHoweverMuchXpItCarries(int level, double xp)
    {
        // Pinned as a pair on purpose: the old predicate's answer is the bug, the new one is the
        // fix. If someone ever routes the clamp back through the weaker test, this fails loudly.
        Assert.False(ProfileSyncService.ServerProfileLooksUninitialized(level, xp));
        Assert.True(ProfileSyncService.ServerProfileTooEmptyToClampTo(level));
    }

    [Theory]
    [InlineData(0)]  // server omitted level entirely
    [InlineData(1)]  // pristine, or emptied
    public void AnEmptyOrPristineRowIsNeverClampedTo(int level)
        => Assert.True(ProfileSyncService.ServerProfileTooEmptyToClampTo(level));

    [Theory]
    [InlineData(2)]
    [InlineData(40)]
    [InlineData(199)]
    public void ARowWithARealLevelStaysClampable(int level)
    {
        // The clamp is not disabled — a genuine server-side correction (Level 40 local edited up
        // to Level 90) still lands, because the server's record has a real level to clamp to.
        Assert.False(ProfileSyncService.ServerProfileTooEmptyToClampTo(level));
    }

    [Fact]
    public void TheClampGuardStrictlySubsumesTheUninitializedGuard()
    {
        // Every record the weaker predicate refuses, the clamp guard also refuses — the clamp
        // sites lost no protection by switching over, they only gained the Level 1 / >=100 XP
        // band. Stated as a property so a future edit to either predicate that breaks the
        // containment shows up here rather than as a fresh #865 report.
        foreach (var level in new[] { 0, 1, 2, 5, 40 })
        foreach (var xp in new[] { 0d, 99d, 100d, 150d, 250_000d })
        {
            if (ProfileSyncService.ServerProfileLooksUninitialized(level, xp))
                Assert.True(ProfileSyncService.ServerProfileTooEmptyToClampTo(level));
        }
    }
}
