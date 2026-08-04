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
}
