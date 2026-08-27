using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1027 (BUG-BN8X9B9SZ5), part two. The ledger itself already survives a logout - quests.json is
/// stamped with its owner instead of wiped - but the ENTITLEMENT providers are torn down by the
/// same logout, so during the signed-out window the premium gate answers "no access" on behalf of
/// nobody. The premium-loss reroll believed that answer and swapped the departed account's
/// untouched premium quest for a free one, which reaches the user as the same "my quests changed
/// when I signed back in" report. QuestService now treats the whole window as unresolved.
/// </summary>
public class QuestSignedOutEntitlementTests
{
    [Fact]
    public void SignedOut_WithOwnedLedger_IsNotAnEntitlementAnswer()
        => Assert.True(QuestService.IsSignedOutWithOwnedQuests(null, "unified-abc"));

    [Fact]
    public void SignedOut_WithEmptyStringId_CountsAsSignedOut()
        => Assert.True(QuestService.IsSignedOutWithOwnedQuests("", "unified-abc"));

    [Fact]
    public void SignedIn_IsAlwaysAnAnswer()
        => Assert.False(QuestService.IsSignedOutWithOwnedQuests("unified-abc", "unified-abc"));

    [Fact]
    public void SignedIn_AsADifferentAccount_IsStillAnAnswer()
    {
        // EnsureOwnedBy wipes the foreign ledger on this login; the entitlement read is the
        // incoming account's own and must not be deferred.
        Assert.False(QuestService.IsSignedOutWithOwnedQuests("unified-new", "unified-old"));
    }

    [Fact]
    public void NeverLoggedIn_IsUnaffected()
    {
        // A user who has never signed in has an unstamped ledger. The #889 settle-window
        // behaviour has to stay exactly as it was for them - no deferral, ever.
        Assert.False(QuestService.IsSignedOutWithOwnedQuests(null, null));
        Assert.False(QuestService.IsSignedOutWithOwnedQuests(null, ""));
    }

    [Fact]
    public void FreshLedger_IsUnstamped()
        => Assert.True(string.IsNullOrEmpty(new QuestProgress().OwnerUnifiedId));
}

/// <summary>
/// Sarah, #general 08-22 (Mort verified): the Sissy Hypno mod's subliminal pool was still
/// conditioning on BambiSleep's named triggers. The pool had been produced by find-replacing the
/// word "BAMBI" out of the BambiSleep pool, which stripped the brand but left the trigger corpus.
/// </summary>
public class SissySubliminalPoolTests
{
    /// <summary>
    /// The BambiSleep trigger phrases that were removed. Eight are verbatim BambiSleep defaults;
    /// UNIFORM LOCK is BAMBI UNIFORM LOCK with the brand filed off, which is why it is listed
    /// separately below - the automatic migration cannot see that one.
    /// </summary>
    public static TheoryData<string> RemovedBambiTriggers => new()
    {
        "DROP FOR COCK", "SNAP AND FORGET", "PRIMPED AND PAMPERED", "ZAP COCK DRAIN OBEY",
        "GIGGLETIME", "UNIFORM LOCK", "COCK ZOMBIE NOW", "COCK TURNS MY BRAIN OFF",
        "I CANT RESIST MY TRIGGERS",
    };

    /// <summary>The subset that is still a verbatim BambiSleep default.</summary>
    public static TheoryData<string> RemovedAndStillBambiDefaults => new()
    {
        "DROP FOR COCK", "SNAP AND FORGET", "PRIMPED AND PAMPERED", "ZAP COCK DRAIN OBEY",
        "GIGGLETIME", "COCK ZOMBIE NOW", "COCK TURNS MY BRAIN OFF", "I CANT RESIST MY TRIGGERS",
    };

    [Theory]
    [MemberData(nameof(RemovedBambiTriggers))]
    public void SissyPool_NoLongerCarriesTheBambiTrigger(string phrase)
    {
        Assert.NotNull(BuiltInMods.SissyHypno.SubliminalPool);
        Assert.DoesNotContain(phrase, BuiltInMods.SissyHypno.SubliminalPool!.Keys);
    }

    [Theory]
    [MemberData(nameof(RemovedAndStillBambiDefaults))]
    public void RemovedPhrase_IsStillABambiDefault_SoExistingPoolsGetPruned(string phrase)
    {
        // The migration for users who already have a saved Sissy pool depends on this: prune
        // only fires for a key that is some OTHER built-in mod's default and not the active
        // mod's. If BambiSleep ever drops one of these, the stale key would go unpruned.
        Assert.NotNull(BuiltInMods.BambiSleep.SubliminalPool);
        Assert.Contains(phrase, BuiltInMods.BambiSleep.SubliminalPool!.Keys);
    }

    [Fact]
    public void UniformLock_IsTheOneThePruneCannotReach()
    {
        // BAMBI UNIFORM LOCK -> UNIFORM LOCK: the de-prefixed form is nobody's default, so
        // PruneCrossModSubliminals sees a user-added phrase and leaves it. Documented, not fixed:
        // stripping it would need a rename list, and one stale key in an already-customised pool
        // is a smaller harm than a pass that can delete phrases a user typed themselves.
        Assert.DoesNotContain("UNIFORM LOCK", BuiltInMods.BambiSleep.SubliminalPool!.Keys);
        Assert.Contains("BAMBI UNIFORM LOCK", BuiltInMods.BambiSleep.SubliminalPool!.Keys);
    }

    /// <summary>
    /// The replacements are reused Sissy-mod lines, not new writing. Every one of them must
    /// already appear somewhere else in the SAME manifest.
    /// </summary>
    public static TheoryData<string> ReusedReplacements => new()
    {
        "GOOD GIRLS OBEY", "EMPTY AND OBEDIENT", "SISSY IS LEARNING", "I LOVE BEING PROGRAMMED",
        "SISSY LOVES BUBBLES", "SISSY WILL TRY HARDER", "DUMB DOLLS COUNT SLOWLY",
        "GOOD GIRLS PAY ATTENTION", "SISSY NEEDS TO FOCUS",
    };

    [Theory]
    [MemberData(nameof(ReusedReplacements))]
    public void Replacement_IsInThePoolNow(string phrase)
        => Assert.Contains(phrase, BuiltInMods.SissyHypno.SubliminalPool!.Keys);

    [Theory]
    [MemberData(nameof(ReusedReplacements))]
    public void Replacement_AlreadyExistedElsewhereInTheSissyMod(string phrase)
    {
        var mod = BuiltInMods.SissyHypno;
        var elsewhere = new List<string>();
        if (mod.LockCardPhrases != null) elsewhere.AddRange(mod.LockCardPhrases.Keys);
        if (mod.Phrases != null) foreach (var set in mod.Phrases.Values) elsewhere.AddRange(set);
        Assert.Contains(phrase, elsewhere);
    }

    [Fact]
    public void SissyPool_KeptItsSize()
    {
        // Nine out, nine in: the subliminal cadence a user already tuned does not change.
        Assert.Equal(21, BuiltInMods.SissyHypno.SubliminalPool!.Count);
    }
}

/// <summary>
/// FYP volume (suggestion thread 1539830425948524654). The feed only ever had a binary mute; the
/// slider persists through the same host bridge every other feed setting uses.
/// </summary>
public class FypVolumeSettingTests
{
    [Fact]
    public void DefaultsToFullVolume() => Assert.Equal(100, new AppSettings().FypVolume);

    [Theory]
    [InlineData(-40, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(9999, 100)]
    public void IsClampedToZeroHundred(int input, int expected)
        => Assert.Equal(expected, new AppSettings { FypVolume = input }.FypVolume);

    [Fact]
    public void MuteAndVolumeAreIndependent()
    {
        // Mute is the panic switch; unmuting has to give back the loudness you had.
        var s = new AppSettings { FypVolume = 30, FypMuted = true };
        s.FypMuted = false;
        Assert.Equal(30, s.FypVolume);
    }
}
