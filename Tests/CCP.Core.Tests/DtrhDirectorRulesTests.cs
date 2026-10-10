using System;
using System.IO;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane c2, the web descent's desktop rules (WPF Services/Chaos/DtrhHostService.cs 7.1.5): the page's
/// bark events map onto the bark triggers and context keys the mods' rules match (RouteBark :749 +
/// BarkService.NotifyChaos*), a run's video starts at a random point that leaves 15 s (VideoPayload,
/// VideoService.ArmRandomSegment), and the session stats store sums runs (DtrhSessionStatsStore).</summary>
[Collection(SessionStatics.Name)]
public sealed class DtrhDirectorRulesTests
{
    private static DtrhBarkRouter.Bark? Route(string json) => DtrhBarkRouter.Route(JObject.Parse(json));

    [Fact]
    public void Bark_events_map_to_the_WPF_triggers_and_context_keys()
    {
        var d = Route("{'event':'defused','combo':7,'variant':'flash','difficulty':'Gentle'}")!;
        Assert.Equal("ChaosBubbleDefused", d.Trigger);
        Assert.Equal(7d, d.Values["combo"]);
        Assert.Equal("flash", d.Values["payload"]);
        Assert.Equal("Gentle", d.Values["difficulty"]);

        var a = Route("{'event':'detonated-absorbed','variant':'video','strength':60,'runDetonations':2,'combo':3,'shields':1}")!;
        Assert.Equal("ChaosBubbleDetonatedAbsorbed", a.Trigger);
        Assert.Equal(true, a.Values["shield_absorbed"]);
        Assert.Equal(1d, a.Values["shields_left"]);
        Assert.Equal(2d, a.Values["run_detonations"]);

        var r = Route("{'event':'rabbit-caught','gold':5}")!;   // reuses the rabbit-catch voice
        Assert.Equal("ChaosDarterCaught", r.Trigger);
        Assert.Equal(5d, r.Values["points"]);
        Assert.Equal(true, r.Values["quick"]);

        Assert.Equal("ChaosFirstTime", Route("{'event':'crafted','id':'x'}")!.Trigger);
        Assert.Equal("ChaosEndingSoon", Route("{'event':'ending-soon'}")!.Trigger);
        Assert.Equal("denied_count", Assert.Single(Route("{'event':'tease-denied','count':2}")!.Values).Key);
        Assert.Equal("ChaosRunStarted", DtrhBarkRouter.RunStarted("Gentle").Trigger);
    }

    [Theory]
    [InlineData("{'event':'effect-fired'}")]   // haptics only: she has nothing to say
    [InlineData("{'event':'made-up'}")]
    [InlineData("{}")]
    public void Voiceless_and_unknown_events_route_nowhere(string json) => Assert.Null(Route(json));

    [Fact]
    public void Video_slice_starts_at_a_random_point_that_leaves_fifteen_seconds()
    {
        Assert.Equal(15, ChaosVideoSegment.SEGMENT_SEC);
        Assert.Equal(0, ChaosVideoSegment.StartMs(15_000, 15, 0.9));      // no longer than the slice: from the start
        Assert.Equal(0, ChaosVideoSegment.StartMs(60_000, 15, 0.01));     // within half a second: not worth a seek
        Assert.Equal(22_500, ChaosVideoSegment.StartMs(60_000, 15, 0.5));
        Assert.Equal(45_000, ChaosVideoSegment.StartMs(60_000, 15, 1.0)); // never closer than 15 s to the end
        var t = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ChaosVideoSegment.StillArmed(t, t.AddSeconds(29)));
        Assert.False(ChaosVideoSegment.StillArmed(t, t.AddSeconds(30)));
        Assert.False(ChaosVideoSegment.StillArmed(DateTime.MinValue, t));
    }

    [Fact]
    public void Session_stats_sum_across_runs_and_keep_the_best_combo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-c2-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DtrhSessionStatsStore.PathOverride = dir;
            DtrhSessionStatsStore.Record(JObject.Parse("{'bubblesPopped':10,'bestCombo':9,'subliminalsHeard':2,'videosShown':1,'effectsByKind':{'flash':3}}"), "Gentle");
            var life = DtrhSessionStatsStore.Record(JObject.Parse("{'bubblesPopped':5,'bestCombo':4,'sparksEarned':7,'xpEarned':12.5,'effectsByKind':{'FLASH':1}}"), "");
            Assert.Equal(2, life.Runs);
            Assert.Equal(15, life.BubblesPopped);
            Assert.Equal(9, life.BestComboEver);
            Assert.Equal(2, life.SubliminalsHeard);
            Assert.Equal(1, life.VideosShown);
            Assert.Equal(7, life.SparksEarned);
            Assert.Equal(12.5, life.XpEarned);
            Assert.Equal(4, life.EffectsByKind["flash"]);
        }
        finally
        {
            DtrhSessionStatsStore.PathOverride = null;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
