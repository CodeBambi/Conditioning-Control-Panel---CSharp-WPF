using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #865 - the XP regression watermark (DRAFT, pending owner review).
///
/// The watermark is the last cumulative total this CLIENT and the SERVER agreed on for an account
/// in a season. ProfileSyncService refuses to push a total below it, on the grounds that a local
/// file which fell below a figure both sides accepted has lost progress rather than learnt
/// something. It is only ever written from a server response, never from a local calculation, so a
/// settings file emptied by a crashed update cannot move it.
///
/// "Last AGREED", not "highest ever reported", is the load-bearing part and the reason this file
/// was rewritten. The first draft only let the number rise while the send-guard enforced it as the
/// floor for outgoing pushes - two different meanings in one field. The moment a client
/// legitimately adopted a LOWER server figure (an anti-cheat clamp, which the sync path does on
/// purpose) the watermark stayed at the old high and every later sync failed against it. And
/// because the send-guard sits in front of the POST, that blocked the whole payload - achievements,
/// quests, cosmetics - pinned "Cloud sync issue" in the title bar, and survived restarts. A latched
/// guard is worse than the regression it was written for, so agreement now moves the number in both
/// directions and the guard self-heals.
///
/// The scoping tests carry the same weight for the same reason: a watermark that outlives its
/// (account, season) scope blocks the resets it must let through.
/// </summary>
public class ProfileSyncXpWatermarkTests
{
    private const string Account = "acct-1";
    private const string Season = "2026-08";

    /// <summary>Arm the watermark by agreeing with the server at <paramref name="xp"/>.</summary>
    private static AppSettings Agreed(double xp, string account = Account, string season = Season)
    {
        var s = new AppSettings { UnifiedId = account, CurrentSeason = season };
        // clientTotalXp == serverTotalXp: the client adopted, so the two agree.
        ProfileSyncService.RecordAgreedServerXp(s, xp, xp, "test");
        return s;
    }

    // ---- recording ------------------------------------------------------------------------

    [Fact]
    public void AgreeingWithTheServerArmsTheWatermarkInTheCurrentScope()
    {
        var s = Agreed(250_000);

        Assert.Equal(250_000, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(Account, s.LastConfirmedServerXpAccount);
        Assert.Equal(Season, s.LastConfirmedServerXpSeason);
    }

    [Fact]
    public void AgreementMovesTheWatermarkDownAsWellAsUp()
    {
        // THE fix. The old rule refused to lower the figure, so an adopted anti-cheat correction
        // left a floor the client could never reach again.
        var s = Agreed(250_000);

        ProfileSyncService.RecordAgreedServerXp(s, 120_000, 120_000, "clamp adopt");
        Assert.Equal(120_000, ProfileSyncService.ActiveXpWatermark(s));

        ProfileSyncService.RecordAgreedServerXp(s, 400_000, 400_000, "server ahead");
        Assert.Equal(400_000, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void KeepingAHigherLocalIsNotAgreementAndLeavesTheWatermarkAlone()
    {
        // The clamp's DEFEND branch and take-higher both end with the client holding more than the
        // server reported. That is a disagreement: the previously agreed figure still stands, and
        // an emptied server row cannot disarm the guard by being read.
        var s = Agreed(250_000);

        ProfileSyncService.RecordAgreedServerXp(s, 150, clientTotalXp: 250_000, "defended");

        Assert.Equal(250_000, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void ATrivialConfirmationNeverArmsTheGuard()
    {
        // Below the meaningful-progress floor there is nothing worth defending, and arming on a
        // brand-new account's first sync would only create false positives.
        var s = new AppSettings { UnifiedId = Account, CurrentSeason = Season };

        ProfileSyncService.RecordAgreedServerXp(s, ProfileSyncService.MeaningfulProgressXp - 1, 0, "test");

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void AFreshInstallCarriesNoWatermark()
    {
        // Every existing install upgrades into this state, so the guard starts disarmed and only
        // arms once a server response has actually been agreed with.
        var s = new AppSettings();

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Null(s.LastConfirmedServerXpAccount);
        Assert.Null(s.LastConfirmedServerXpSeason);
    }

    // ---- the send-guard scenario this all exists for ---------------------------------------

    [Fact]
    public void ADownwardAdoptLeavesTheNextPushAbleToSend()
    {
        // End-to-end shape of the bug, in the terms the send-guard checks:
        // agree at 250k -> the server clamps us to 120k and we adopt -> the next sync wants to
        // push 120k. Under the old monotonic rule the watermark was still 250k and that push (and
        // every push after it, forever, carrying the entire payload) was refused.
        var s = Agreed(250_000);

        ProfileSyncService.RecordAgreedServerXp(s, 120_000, 120_000, "anti-cheat clamp");

        var wouldPush = 120_000d;
        var watermark = ProfileSyncService.ActiveXpWatermark(s);
        Assert.False(watermark > 0 && wouldPush < watermark);
    }

    [Fact]
    public void ALocalProfileThatLostProgressIsStillRefused()
    {
        // The guard must still do its job: nothing agreed the total down, the local file simply
        // fell. That push stays blocked.
        var s = Agreed(250_000);

        var wouldPush = 4_000d;
        var watermark = ProfileSyncService.ActiveXpWatermark(s);
        Assert.True(watermark > 0 && wouldPush < watermark);
    }

    // ---- scoping --------------------------------------------------------------------------

    [Fact]
    public void ASeasonRolloverPutsTheWatermarkOutOfScope()
    {
        var s = Agreed(250_000);

        s.CurrentSeason = "2026-09";

        // Seasonal XP is SUPPOSED to fall here. An in-scope watermark would refuse the rollover
        // forever, which is worse than the regression it exists to catch.
        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void AnAccountSwitchPutsTheWatermarkOutOfScope()
    {
        var s = Agreed(250_000);

        s.UnifiedId = "acct-2";

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void ReArmingAfterARolloverStartsFromTheNewSeasonsFigure()
    {
        var s = Agreed(250_000);
        s.CurrentSeason = "2026-09";

        // Post-rollover the server reports a small seasonal total. It re-scopes rather than
        // merging, so last season's 250k cannot leak forward and block this season.
        ProfileSyncService.RecordAgreedServerXp(s, 3_000, 3_000, "test");

        Assert.Equal(3_000, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal("2026-09", s.LastConfirmedServerXpSeason);
    }

    // ---- V1 / legacy identities (B-4) -------------------------------------------------------

    [Fact]
    public void ALegacyUserWithNoUnifiedIdIsNeverArmed()
    {
        // A V1 user has neither a unified_id nor a season key, so their scope would be the pair
        // ("", ""): never invalidated by a rollover, and shared with every other legacy account on
        // the machine. Refuse to arm rather than hand them a guard with no escape hatch.
        var s = new AppSettings { UnifiedId = null, CurrentSeason = null };

        ProfileSyncService.RecordAgreedServerXp(s, 250_000, 250_000, "V1 merge");

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(0, s.LastConfirmedServerXp);
    }

    [Fact]
    public void ALegacySeasonalResetIsNeverBlocked()
    {
        // The scenario B-4 is about: a legacy user's season resets, their total drops, and there
        // is no season key for the scoping to notice. With no watermark ever armed, the send-guard
        // has nothing to refuse and the reset syncs normally.
        var s = new AppSettings { UnifiedId = string.Empty, CurrentSeason = string.Empty };
        ProfileSyncService.RecordAgreedServerXp(s, 250_000, 250_000, "V1 merge");

        // ...season rolls over server-side; local total falls to a fresh seasonal figure.
        var wouldPush = 500d;
        var watermark = ProfileSyncService.ActiveXpWatermark(s);

        Assert.Equal(0, watermark);
        Assert.False(watermark > 0 && wouldPush < watermark);
    }

    [Fact]
    public void AStoredWatermarkIsIgnoredOnceTheIdentityIsLegacy()
    {
        // Defensive: covers settings persisted by an earlier build of this draft, which did arm
        // ("", "") scopes. Losing the unified_id must not leave a live guard behind.
        var s = Agreed(250_000);

        s.UnifiedId = null;

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    // ---- clearing -------------------------------------------------------------------------

    [Fact]
    public void ClearingVoidsAllThreeFields()
    {
        var s = Agreed(250_000);

        ProfileSyncService.ClearXpWatermark(s, "test");

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.Null(s.LastConfirmedServerXpAccount);
        Assert.Null(s.LastConfirmedServerXpSeason);
    }

    [Fact]
    public void ClearingIsSafeOnANullSettingsObject()
        => ProfileSyncService.ClearXpWatermark(null, "test");   // must not throw

    // ---- the mid-season admin reset (B-2) ---------------------------------------------------

    [Fact]
    public void AMidSeasonAdminResetClearsTheWatermarkAndThePushGoesThrough()
    {
        // The case that had no test and no working path. An admin sends level_reset mid-season:
        // same season key, so the rollover escape does NOT fire, and the reset zeroes a profile
        // the watermark says was Level 47.
        //
        // The old code asked RefuseToZeroAConfirmedProfile inside the `if`, which refused - and
        // refused permanently, because level_reset is one-shot from the server. Worse, the refusal
        // fell through into the clamp chain, where the zeroed row reads as uninitialized, local was
        // kept, and the next push wrote the pre-reset profile back over the admin's work.
        //
        // An explicit level_reset is the server EXPLAINING the zeroing, so the sync path now clears
        // the watermark and adopts. Modelled here as the two operations that branch performs.
        var s = Agreed(250_000);
        s.PlayerLevel = 47;

        ProfileSyncService.ClearXpWatermark(s, "admin level_reset");
        s.PlayerLevel = 1;
        s.PlayerXP = 0;

        // Nothing left to block the push that carries the reset upward.
        var watermark = ProfileSyncService.ActiveXpWatermark(s);
        Assert.Equal(0, watermark);
        Assert.False(watermark > 0 && 0d < watermark);
    }

    [Fact]
    public void ReArmingAfterAnAdminResetStartsFromTheResetFigure()
    {
        // And the guard comes back as soon as there is something to defend again, in the SAME
        // season - the reset did not cost the account its protection for the rest of the month.
        var s = Agreed(250_000);
        ProfileSyncService.ClearXpWatermark(s, "admin level_reset");

        ProfileSyncService.RecordAgreedServerXp(s, 8_000, 8_000, "V2 sync");

        Assert.Equal(8_000, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(Season, s.LastConfirmedServerXpSeason);
    }
}
