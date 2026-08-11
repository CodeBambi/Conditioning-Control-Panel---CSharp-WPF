using System;
using System.Linq;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — structural recommendation dedupe (doc 01 §3.4). This replaces the per-call
/// Fisher-Yates shuffle of example titles, which was the #1 provider-cache killer AND a weaker
/// anti-fixation fix. If this list is wrong the companion re-pitches the same video all evening,
/// which is the exact failure that forced ambient calls stateless in the first place.
/// </summary>
public class RecentRecommendationsTests
{
    private sealed class FakeClock
    {
        public DateTime Now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Get() => Now;
    }

    [Fact]
    public void Note_TracksNewestFirst()
    {
        var recs = new RecentRecommendations();
        recs.Note("Bambi Bae");
        recs.Note("Deep Trance Programming");

        Assert.Equal(new[] { "Deep Trance Programming", "Bambi Bae" }, recs.Current().ToArray());
    }

    [Fact]
    public void Note_KeepsOnlyMaxTracked()
    {
        var recs = new RecentRecommendations();
        int overfill = RecentRecommendations.MaxTracked + 3;
        for (int i = 1; i <= overfill; i++) recs.Note($"video {i}");

        var current = recs.Current();
        Assert.Equal(RecentRecommendations.MaxTracked, current.Count);
        Assert.Equal($"video {overfill}", current[0]);
        Assert.DoesNotContain("video 1", current);
        Assert.DoesNotContain("video 3", current);
    }

    [Fact]
    public void Note_DedupesCaseInsensitively_AndRefreshesInsteadOfConsumingASlot()
    {
        // One repeated title must not be able to evict the whole ban list — that would hand the
        // model back the very fixation this exists to prevent.
        var recs = new RecentRecommendations();
        recs.Note("Bambi Bae");
        recs.Note("Sleepy Spiral");
        recs.Note("bambi bae");

        var current = recs.Current();
        Assert.Equal(2, current.Count);
        Assert.Equal("bambi bae", current[0]);   // refreshed to newest
        Assert.Equal("Sleepy Spiral", current[1]);
    }

    [Fact]
    public void Note_IgnoresBlankTitles()
    {
        var recs = new RecentRecommendations();
        recs.Note(null);
        recs.Note("");
        recs.Note("   ");

        Assert.Empty(recs.Current());
        Assert.Null(recs.BuildExclusionLine());
    }

    [Fact]
    public void Entries_ExpireAfterTheTwentyFourHourTtl()
    {
        var clock = new FakeClock();
        var recs = new RecentRecommendations(clock.Get);

        recs.Note("Bambi Bae");
        clock.Now = clock.Now.AddHours(23).AddMinutes(59);
        recs.Note("Sleepy Spiral");

        Assert.Equal(2, recs.Current().Count);

        // Push past 24h from the FIRST note only.
        clock.Now = clock.Now.AddMinutes(2);
        var current = recs.Current();

        Assert.Single(current);
        Assert.Equal("Sleepy Spiral", current[0]);
    }

    [Fact]
    public void BuildExclusionLine_IsNullWhenEmpty_AndListsTitlesOtherwise()
    {
        var recs = new RecentRecommendations();
        Assert.Null(recs.BuildExclusionLine());

        recs.Note("Bambi Bae");
        recs.Note("Sleepy Spiral");

        var line = recs.BuildExclusionLine();
        Assert.NotNull(line);
        Assert.StartsWith("Already suggested recently (pick something else): ", line);
        Assert.Contains("Sleepy Spiral", line);
        Assert.Contains("Bambi Bae", line);
        // Exactly one line — it lives in the small dynamic tail, not the cached prefix.
        Assert.DoesNotContain("\n", line);
    }

    [Fact]
    public void Clear_EmptiesTheBanList()
    {
        var recs = new RecentRecommendations();
        recs.Note("Bambi Bae");
        recs.Clear();

        Assert.Empty(recs.Current());
        Assert.Null(recs.BuildExclusionLine());
    }
}
