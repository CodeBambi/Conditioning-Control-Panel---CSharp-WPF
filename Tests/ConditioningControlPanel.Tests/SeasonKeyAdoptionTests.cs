using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The August 1 2026 season rollover produced no recap card for anyone who did not re-login
/// across the boundary. The server rolled the season correctly; the /v2/profile/sync response
/// that performed the rollover just never mentioned the new key, and nothing on the sync path
/// wrote AppSettings.CurrentSeason. SeasonRecapService.CurrentSeasonKey prefers that stored
/// value over wall-clock, so the recap compared "2026-07" against "2026-07", concluded nothing
/// had happened, cleared its pending latch and returned - without logging.
///
/// ShouldAdoptServerSeason is the guard that decides when to take a server key. It has to be
/// permissive enough to unstick that state and strict enough not to undo the two July fixes
/// that stopped the recap firing early (premature wall-clock roll) or twice (backward desync).
/// </summary>
public class SeasonKeyAdoptionTests
{
    [Fact]
    public void AdvancingKeyIsAdopted()
        => Assert.True(SeasonRecapService.ShouldAdoptServerSeason("2026-08", "2026-07"));

    [Fact]
    public void TheAugustFirstCaseIsUnstuck()
    {
        // The exact stuck state: local believes July, server has rolled to August.
        Assert.True(SeasonRecapService.ShouldAdoptServerSeason("2026-08", "2026-07"));
        // ...and once adopted, re-running the same sync must be a no-op rather than re-firing.
        Assert.False(SeasonRecapService.ShouldAdoptServerSeason("2026-08", "2026-08"));
    }

    [Fact]
    public void YearBoundaryAdvances()
        => Assert.True(SeasonRecapService.ShouldAdoptServerSeason("2027-01", "2026-12"));

    [Theory]
    [InlineData("2026-07", "2026-07")] // same season - nothing happened
    [InlineData("2026-06", "2026-07")] // backward: desync or stale read
    [InlineData("2025-12", "2026-01")] // backward across a year
    public void NonAdvancingKeyIsIgnored(string server, string local)
        => Assert.False(SeasonRecapService.ShouldAdoptServerSeason(server, local));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-8")]      // not zero-padded, so ordinal compare would misorder it
    [InlineData("2026/08")]
    [InlineData("august")]
    [InlineData("2026-08-01")]  // a full date, not a month key
    public void MalformedOrAbsentServerKeyIsNeverAdopted(string? server)
    {
        // An older server omits the field entirely. Adopting garbage would poison CurrentSeasonKey,
        // which is compared with CompareOrdinal against real keys.
        Assert.False(SeasonRecapService.ShouldAdoptServerSeason(server, "2026-07"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-key")]
    public void UnusableLocalKeyAcceptsAnyWellFormedServerKey(string? local)
    {
        // Fresh install, or a local value we cannot order against. Taking the server's key is
        // strictly better than leaving CurrentSeasonKey to fall through to wall-clock.
        Assert.True(SeasonRecapService.ShouldAdoptServerSeason("2026-08", local));
    }

    [Fact]
    public void UnusableLocalKeyStillRejectsAnUnusableServerKey()
        => Assert.False(SeasonRecapService.ShouldAdoptServerSeason("junk", "junk"));

    // ---- ShouldAdoptSilently: the fresh-device case ----
    //
    // A returning user signing in on a NEW PC has a settings file with no season keys in it at
    // all. The first sync then hands over the server's real key, and everything downstream reads
    // the empty LastSeasonResetSeen as "earlier than" that key and announces a rollover - one this
    // machine never witnessed, with no snapshot behind it, so the card it opens is empty. This
    // helper is the one gate that separates "I have never seen a season" from "the season I saw
    // has ended", and both halves matter: too loose and a real rollover is swallowed, too tight
    // and the false box comes back.

    [Fact]
    public void FreshSettingsWithAServerConfirmedKeyAdoptsSilently()
    {
        // Exactly the new-PC case: nothing local, the server named the season.
        Assert.True(SeasonRecapService.ShouldAdoptSilently("", "", serverConfirmed: true, resetPending: false));
        Assert.True(SeasonRecapService.ShouldAdoptSilently(null, null, serverConfirmed: true, resetPending: false));
    }

    [Fact]
    public void FreshSettingsOnWallClockDoesNotAdopt()
    {
        // Not logged in / sync failing: CurrentSeasonKey is the wall-clock fallback, a key no
        // server ever authored. Writing that into settings would invent a season boundary AND
        // suppress the first genuine rollover this install ever sees.
        Assert.False(SeasonRecapService.ShouldAdoptSilently("", "", serverConfirmed: false, resetPending: false));
        Assert.False(SeasonRecapService.ShouldAdoptSilently(null, null, serverConfirmed: false, resetPending: false));
    }

    [Theory]
    [InlineData("2026-08", "")]        // recapped before; the stats bucket happens to be empty
    [InlineData("", "2026-08")]        // never recapped, but this install has been accruing stats
    [InlineData("2026-08", "2026-08")]  // an ordinary established install
    public void AnInstallThatHasSeenASeasonNeverAdoptsSilently(string lastSeasonSeen, string statsSeason)
    {
        // Either key being set means a season was witnessed here, so a later advance is a REAL
        // rollover and has to reach the recap. Silently adopting would eat it.
        Assert.False(SeasonRecapService.ShouldAdoptSilently(lastSeasonSeen, statsSeason, serverConfirmed: true, resetPending: false));
    }

    [Fact]
    public void SilentAdoptIsOneShot()
    {
        // After the adopt writes both keys, the very next call must decline - otherwise the block
        // would re-run on every launch and keep clearing SeasonResetPending under a real reset.
        Assert.True(SeasonRecapService.ShouldAdoptSilently("", "", serverConfirmed: true, resetPending: false));
        Assert.False(SeasonRecapService.ShouldAdoptSilently("2026-09", "2026-09", serverConfirmed: true, resetPending: false));
    }

    [Fact]
    public void AServerResetIsNeverAdoptedAway()
    {
        // The one case the fresh-settings shape does NOT cover. SeasonResetPending is written only
        // by ProfileSyncService, off an explicit server level_reset - an admin resetting a single
        // account, which is the only way a reset surfaces mid-month and the only way the feature is
        // testable at all. The silent adopt cleared that latch on its way past and returned, so on
        // an install with both keys empty the reset was swallowed without a word. Real news beats
        // "I have never seen a season": fall through and let the pending path speak.
        Assert.False(SeasonRecapService.ShouldAdoptSilently("", "", serverConfirmed: true, resetPending: true));
        Assert.False(SeasonRecapService.ShouldAdoptSilently(null, null, serverConfirmed: true, resetPending: true));
    }

    [Fact]
    public void AResetWithNoServerSeasonIsStillNotAdopted()
        // Both reasons to refuse at once. Nothing here should be able to cancel the other out.
        => Assert.False(SeasonRecapService.ShouldAdoptSilently("", "", serverConfirmed: false, resetPending: true));

    [Fact]
    public void WhitespaceIsNotEmpty()
    {
        // IsNullOrEmpty, not IsNullOrWhiteSpace, on purpose: a whitespace key is a corrupted
        // value, not a virgin install, and it is what CompareOrdinal will actually be handed
        // downstream. Leave it to the rollover path rather than papering over it here.
        Assert.False(SeasonRecapService.ShouldAdoptSilently("   ", "", serverConfirmed: true, resetPending: false));
    }
}
