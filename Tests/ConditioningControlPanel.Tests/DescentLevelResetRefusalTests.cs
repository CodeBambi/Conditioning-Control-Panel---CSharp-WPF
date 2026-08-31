using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// On 2026-09-01 the Descent ended monthly seasons permanently. Server-side a DESCENT_MIGRATION
/// flag suppresses every future wipe - but suppression that fails open is still a wipe, and the
/// client's level_reset branch used to obey one unconditionally: it cleared the XP watermark,
/// adopted the server's zeroes, dropped the mechanical skill tree, and then pushed the whole
/// thing back up as its own agreed truth. After that round trip there is nothing left to restore
/// from on either side.
///
/// RefuseDescentEraLevelReset is the client's own refusal, and it is deliberately pure: no App,
/// no settings, no clock of its own, so the exact conditions can be pinned here instead of being
/// reachable only through a live sync.
///
/// The asymmetry that justifies every "refuse" below: a wrong refusal costs an admin re-running a
/// reset by hand after reading a loud log line. A wrong acceptance costs a user their level and
/// their XP, permanently and silently.
/// </summary>
public class DescentLevelResetRefusalTests
{
    private static readonly DateTime BeforeDescent = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterDescent = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    // ---------- the ordinary pre-Descent reset still works ----------

    [Fact]
    public void OrdinaryPreDescentRolloverIsStillObeyed()
    {
        // July -> August, before the epoch, unmigrated account. This is a real season rollover and
        // refusing it would have broken the feature for the whole month before the ceremony.
        Assert.False(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-08", localSeason: "2026-07",
            migrationCompleted: false, nowUtc: BeforeDescent));
    }

    [Fact]
    public void MidSeasonAdminResetIsStillObeyedBeforeTheEpoch()
    {
        // An admin reset carries no season change at all (#865: this is exactly the case an earlier
        // guard refused permanently, because level_reset is one-shot and never re-sent).
        Assert.False(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-08", localSeason: "2026-08",
            migrationCompleted: false, nowUtc: BeforeDescent));
    }

    // ---------- (1) the account has already been through the ceremony ----------

    [Fact]
    public void AMigratedAccountRefusesEvenBeforeTheEpoch()
    {
        // The ceremony is one-way: this account is on curve v2 and seasons are over FOR IT
        // regardless of what the calendar or a lagging server says.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-08", localSeason: "2026-07",
            migrationCompleted: true, nowUtc: BeforeDescent));
    }

    // ---------- (2) the wall clock is past the epoch ----------

    [Fact]
    public void EveryResetAfterTheEpochIsRefused()
    {
        // The failed-open server: still rotating seasons in September as if nothing happened.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-10", localSeason: "2026-09",
            migrationCompleted: false, nowUtc: AfterDescent));
    }

    [Fact]
    public void AnUndateableResetAfterTheEpochIsRefused()
    {
        // No season key anywhere - an old deploy, or a truncated response. "I cannot tell when this
        // is from" is not a reason to zero someone once seasons no longer exist.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: null, localSeason: null,
            migrationCompleted: false, nowUtc: AfterDescent));
    }

    [Fact]
    public void TheEpochInstantItselfRefuses()
    {
        // Boundary: the comparison is >=, so midnight on the day itself is already post-Descent.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-09", localSeason: "2026-08",
            migrationCompleted: false, nowUtc: DescentEpochs.SeasonsEndUtc));

        // ...and the last instant before it does not.
        Assert.False(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: "2026-08", localSeason: "2026-07",
            migrationCompleted: false,
            nowUtc: DescentEpochs.SeasonsEndUtc.AddTicks(-1)));
    }

    // ---------- (3)/(4) the reset dates ITSELF post-Descent, whatever our clock thinks ----------

    [Theory]
    [InlineData("2026-09")]  // the first month that no longer exists
    [InlineData("2026-10")]
    [InlineData("2027-01")]  // across a year boundary
    public void APostDescentServerKeyRefusesEvenOnASlowClock(string serverSeason)
    {
        // A ceremony at 19:00Z with users in every timezone means client clocks WILL disagree with
        // the epoch. If the server is rolling this account into a month on the far side of the
        // Descent, that dates the reset by itself and our clock does not get a vote.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: serverSeason, localSeason: "2026-08",
            migrationCompleted: false, nowUtc: BeforeDescent));
    }

    [Fact]
    public void APostDescentLocalKeyRefusesToo()
    {
        // Same reasoning from the other side: we already hold a post-Descent key, so a reset
        // arriving now cannot be a season rollover no matter what the response claims.
        Assert.True(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: null, localSeason: "2026-09",
            migrationCompleted: false, nowUtc: BeforeDescent));
    }

    // ---------- garbage keys must not decide anything ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-9")]       // not zero-padded: an ordinal compare would sort it AFTER "2026-09"
    [InlineData("2026/09")]
    [InlineData("september")]
    [InlineData("2026-09-01")]   // a full date, not a month key
    public void MalformedKeysAreNotTreatedAsPostDescent(string season)
    {
        // The refusal is a real consequence, so it may never be triggered by a typo or a wire-format
        // change. Note "2026-9" in particular: ordinal string compare puts it after "2026-09", so a
        // predicate without the shape check would refuse every reset for an old non-padded server.
        Assert.False(DescentEpochs.IsPostDescentSeasonKey(season));
        Assert.False(ProfileSyncService.RefuseDescentEraLevelReset(
            serverSeason: season, localSeason: season,
            migrationCompleted: false, nowUtc: BeforeDescent));
    }

    [Fact]
    public void PreDescentKeysAreNotPostDescent()
    {
        Assert.False(DescentEpochs.IsPostDescentSeasonKey(null));
        Assert.False(DescentEpochs.IsPostDescentSeasonKey("2026-08"));
        Assert.False(DescentEpochs.IsPostDescentSeasonKey("2026-02"));
        Assert.True(DescentEpochs.IsPostDescentSeasonKey(DescentEpochs.FirstPostDescentSeasonKey));
    }

    [Fact]
    public void TheEpochIsTheCeremonyDateInUtc()
    {
        // Pinned so a stray edit to the constant fails here rather than in production on the day.
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), DescentEpochs.SeasonsEndUtc);
        Assert.Equal(DateTimeKind.Utc, DescentEpochs.SeasonsEndUtc.Kind);
        Assert.Equal("2026-09", DescentEpochs.FirstPostDescentSeasonKey);
    }
}
