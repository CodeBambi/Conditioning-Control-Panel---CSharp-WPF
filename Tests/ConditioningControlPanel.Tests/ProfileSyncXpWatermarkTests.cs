using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #865 - the XP regression watermark (DRAFT, pending owner review).
///
/// The watermark is the highest cumulative XP the SERVER itself has confirmed for an account, and
/// it is written only from a server response - never from a local calculation. That is the whole
/// point: a settings file emptied by a crashed update cannot raise it, so it survives as evidence
/// of what the account really held. ProfileSyncService then refuses to push a total below it, and
/// refuses to adopt a response that zeroes a profile the watermark says was real.
///
/// It is scoped to (account, season) because both of those legitimately lower XP: a season
/// rollover resets seasonal progress by design, and a different account's totals are unrelated.
/// Most of these tests are about that scoping, because a watermark that outlives its scope blocks
/// the very resets it must let through - a worse failure than the bug it fixes.
/// </summary>
public class ProfileSyncXpWatermarkTests
{
    private const string Account = "acct-1";
    private const string Season = "2026-08";

    private static AppSettings Confirmed(double xp, string account = Account, string season = Season)
    {
        var s = new AppSettings { UnifiedId = account, CurrentSeason = season };
        ProfileSyncService.RecordServerConfirmedXp(s, xp);
        return s;
    }

    // ---- recording ------------------------------------------------------------------------

    [Fact]
    public void AServerConfirmationArmsTheWatermarkInTheCurrentScope()
    {
        var s = Confirmed(250_000);

        Assert.Equal(250_000, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(Account, s.LastConfirmedServerXpAccount);
        Assert.Equal(Season, s.LastConfirmedServerXpSeason);
    }

    [Fact]
    public void TheWatermarkOnlyEverRisesWithinAScope()
    {
        var s = Confirmed(250_000);

        // A later response reporting less (mid-season server hiccup, partial read) must not talk
        // the high-water mark down - that would let the regression it guards against back in.
        ProfileSyncService.RecordServerConfirmedXp(s, 10_000);
        Assert.Equal(250_000, ProfileSyncService.ActiveXpWatermark(s));

        ProfileSyncService.RecordServerConfirmedXp(s, 400_000);
        Assert.Equal(400_000, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void ATrivialConfirmationNeverArmsTheGuard()
    {
        // Below the meaningful-progress floor there is nothing worth defending, and arming on a
        // brand-new account's first sync would only create false positives.
        var s = new AppSettings { UnifiedId = Account, CurrentSeason = Season };

        ProfileSyncService.RecordServerConfirmedXp(s, ProfileSyncService.MeaningfulProgressXp - 1);

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void AFreshInstallCarriesNoWatermark()
    {
        // Every existing install upgrades into this state, so the guard starts disarmed and only
        // arms once a server response has actually confirmed something.
        var s = new AppSettings();

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Null(s.LastConfirmedServerXpAccount);
        Assert.Null(s.LastConfirmedServerXpSeason);
    }

    // ---- scoping --------------------------------------------------------------------------

    [Fact]
    public void ASeasonRolloverPutsTheWatermarkOutOfScope()
    {
        var s = Confirmed(250_000);

        s.CurrentSeason = "2026-09";

        // Seasonal XP is SUPPOSED to fall here. An in-scope watermark would refuse the rollover
        // forever, which is worse than the regression it exists to catch.
        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void AnAccountSwitchPutsTheWatermarkOutOfScope()
    {
        var s = Confirmed(250_000);

        s.UnifiedId = "acct-2";

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
    }

    [Fact]
    public void ReArmingAfterARolloverStartsFromTheNewSeasonsFigure()
    {
        var s = Confirmed(250_000);
        s.CurrentSeason = "2026-09";

        // Post-rollover the server reports a small seasonal total. It re-scopes rather than
        // merging, so last season's 250k cannot leak forward and block this season.
        ProfileSyncService.RecordServerConfirmedXp(s, 3_000);

        Assert.Equal(3_000, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal("2026-09", s.LastConfirmedServerXpSeason);
    }

    // ---- clearing -------------------------------------------------------------------------

    [Fact]
    public void ClearingVoidsAllThreeFields()
    {
        var s = Confirmed(250_000);

        ProfileSyncService.ClearXpWatermark(s, "test");

        Assert.Equal(0, ProfileSyncService.ActiveXpWatermark(s));
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.Null(s.LastConfirmedServerXpAccount);
        Assert.Null(s.LastConfirmedServerXpSeason);
    }

    [Fact]
    public void ClearingIsSafeOnANullSettingsObject()
        => ProfileSyncService.ClearXpWatermark(null, "test");   // must not throw

    // ---- the adopt-side refusal -----------------------------------------------------------

    [Fact]
    public void AnEmptyServerProfileIsRefusedWhileTheWatermarkStands()
    {
        var s = Confirmed(250_000);
        s.PlayerLevel = 47;

        // The account was Level 47 minutes ago by the server's own account of it, and nothing has
        // reset it. An empty record now is a server-side misread, not a reset.
        Assert.True(ProfileSyncService.RefuseToZeroAConfirmedProfile(s, serverLevel: 1, serverTotalXp: 0, "test"));
    }

    [Fact]
    public void ARealServerProfileIsNeverRefused()
    {
        var s = Confirmed(250_000);

        // Lower but real (an anti-cheat clamp, another device) still gets through - the guard is
        // about ZEROING a confirmed profile, not about defending every downward move.
        Assert.False(ProfileSyncService.RefuseToZeroAConfirmedProfile(s, serverLevel: 30, serverTotalXp: 120_000, "test"));
    }

    [Fact]
    public void ASeasonRolloverIsAllowedToZeroTheProfile()
    {
        var s = Confirmed(250_000);
        s.CurrentSeason = "2026-09";   // the sync path adopts the key before applying level_reset

        Assert.False(ProfileSyncService.RefuseToZeroAConfirmedProfile(s, serverLevel: 1, serverTotalXp: 0, "test"));
    }

    [Fact]
    public void AnExplicitClearIsAllowedToZeroTheProfile()
    {
        var s = Confirmed(250_000);
        ProfileSyncService.ClearXpWatermark(s, "logout");

        Assert.False(ProfileSyncService.RefuseToZeroAConfirmedProfile(s, serverLevel: 1, serverTotalXp: 0, "test"));
    }

    [Fact]
    public void WithNoWatermarkNothingIsEverRefused()
    {
        var s = new AppSettings { UnifiedId = Account, CurrentSeason = Season };

        Assert.False(ProfileSyncService.RefuseToZeroAConfirmedProfile(s, serverLevel: 1, serverTotalXp: 0, "test"));
    }
}
